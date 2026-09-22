[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')] [string]$Version,
    [Parameter(Mandatory)] [ValidateSet('Debug', 'Release')] [string]$Configuration,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$PublishDirectory,
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runtimeProject = Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw\SteamInputAddonforClaw.csproj'
$uiProject = Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw.UI\SteamInputAddonforClaw.UI.csproj'
$overlayProject = Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw.Overlay\SteamInputAddonforClaw.Overlay.csproj'
$fseDistribution = Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw.FseHome\Packaging\Distribution'
$runtimeOutput = [System.IO.Path]::GetFullPath($PublishDirectory)
$uiOutput = Join-Path $runtimeOutput 'ui'
$overlayOutput = Join-Path $runtimeOutput 'overlay'
$fseOutput = Join-Path $runtimeOutput 'fse'

if (Test-Path -LiteralPath $runtimeOutput) {
    Remove-Item -LiteralPath $runtimeOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $runtimeOutput -Force | Out-Null
New-Item -ItemType Directory -Path $uiOutput -Force | Out-Null
New-Item -ItemType Directory -Path $overlayOutput -Force | Out-Null
New-Item -ItemType Directory -Path $fseOutput -Force | Out-Null

$commonArguments = @(
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'false',
    "/p:Version=$Version"
)
if ($NoRestore) { $commonArguments += '--no-restore' }

dotnet publish $runtimeProject @commonArguments '--output' $runtimeOutput
if ($LASTEXITCODE -ne 0) { throw "Runtime publish failed with exit code $LASTEXITCODE." }

$uiArguments = @('--configuration', $Configuration, '--runtime', 'win-x64', '--self-contained', 'false', "/p:Version=$Version")
if ($NoRestore) { $uiArguments += '--no-restore' }
dotnet publish $uiProject @uiArguments '--output' $uiOutput
if ($LASTEXITCODE -ne 0) { throw "UI publish failed with exit code $LASTEXITCODE." }

dotnet publish $overlayProject @commonArguments '--output' $overlayOutput
if ($LASTEXITCODE -ne 0) { throw "Overlay publish failed with exit code $LASTEXITCODE." }

foreach ($fseArtifact in @('SteamInputAddonforClaw.FseHome.msix', 'SteamInputAddonforClaw.FseHome.cer')) {
    $source = Join-Path $fseDistribution $fseArtifact
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Fixed FSE distribution artifact was not found: $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $fseOutput $fseArtifact) -Force
}

Write-Host "Published Runtime, external UI, Overlay, and FSE Home layout at $runtimeOutput with version $Version."
