[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$PreserveReleases,

    [string]$ReleaseNotesPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\SteamInputAddonforClaw\SteamInputAddonforClaw.csproj'
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

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    /p:Version=$Version `
    --output $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$vpkArguments = @(
    'vpk', '--version', '1.2.0', 'pack',
    '--packId', 'SteamInputAddonforClaw',
    '--packTitle', 'Steam Input Addon for Claw',
    '--packVersion', $Version,
    '--packDir', $publishDirectory,
    '--mainExe', 'SteamInputAddonforClaw.exe',
    '--outputDir', $releasesDirectory
)

if ($ReleaseNotesPath) {
    $vpkArguments += @('--releaseNotes', $ReleaseNotesPath)
}

dnx @vpkArguments

if ($LASTEXITCODE -ne 0) {
    throw "Velopack packaging failed with exit code $LASTEXITCODE."
}
