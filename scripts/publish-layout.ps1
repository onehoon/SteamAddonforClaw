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
$qamProject = Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw.QamHost\SteamInputAddonforClaw.QamHost.csproj'
$overlayProject = Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw.Overlay\SteamInputAddonforClaw.Overlay.csproj'
$fseProject = Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw.FseHome\SteamInputAddonforClaw.FseHome.csproj'
$runtimeOutput = [System.IO.Path]::GetFullPath($PublishDirectory)
$uiOutput = Join-Path $runtimeOutput 'ui'
$qamOutput = Join-Path $runtimeOutput 'qam'
$overlayOutput = Join-Path $runtimeOutput 'overlay'
$fseOutput = Join-Path $runtimeOutput 'fse'

if (Test-Path -LiteralPath $runtimeOutput) {
    Remove-Item -LiteralPath $runtimeOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $runtimeOutput -Force | Out-Null
New-Item -ItemType Directory -Path $uiOutput -Force | Out-Null
New-Item -ItemType Directory -Path $qamOutput -Force | Out-Null
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

dotnet publish $qamProject @commonArguments '--output' $qamOutput
if ($LASTEXITCODE -ne 0) { throw "QamHost publish failed with exit code $LASTEXITCODE." }

dotnet publish $overlayProject @commonArguments '--output' $overlayOutput
if ($LASTEXITCODE -ne 0) { throw "Overlay publish failed with exit code $LASTEXITCODE." }

dotnet publish $fseProject @commonArguments '--output' $fseOutput
if ($LASTEXITCODE -ne 0) { throw "FSE Home publish failed with exit code $LASTEXITCODE." }

$packageOutput = Join-Path $fseOutput 'Package'
$packagePublicOutput = Join-Path $packageOutput 'Public'
$packageAssetsOutput = Join-Path $packageOutput 'Assets'
New-Item -ItemType Directory -Path $packagePublicOutput, $packageAssetsOutput -Force | Out-Null
Get-ChildItem -LiteralPath $fseOutput -File | Copy-Item -Destination $packageOutput -Force
$packageVersion = (($Version -split '-')[0] + '.0')
$manifest = Get-Content (Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw.FseHome\Packaging\AppxManifest.xml') -Raw
$manifest = $manifest.Replace('__PACKAGE_VERSION__', $packageVersion)
Set-Content -LiteralPath (Join-Path $packageOutput 'AppxManifest.xml') -Value $manifest -Encoding UTF8
Copy-Item (Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw.FseHome\Packaging\CustomCapability.SCCD') (Join-Path $packageOutput 'CustomCapability.SCCD') -Force
Copy-Item (Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw.FseHome\Packaging\Public\README.txt') (Join-Path $packagePublicOutput 'README.txt') -Force
Copy-Item (Join-Path $PSScriptRoot '..\src\SteamInputAddonforClaw\Assets\AppIcon.ico') (Join-Path $packageAssetsOutput 'AppIcon.ico') -Force

Write-Host "Published Runtime, external UI, QAM, Overlay, and FSE Home layout at $runtimeOutput with version $Version."
