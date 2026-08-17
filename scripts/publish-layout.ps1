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
$runtimeOutput = [System.IO.Path]::GetFullPath($PublishDirectory)
$uiOutput = Join-Path $runtimeOutput 'ui'

if (Test-Path -LiteralPath $runtimeOutput) {
    Remove-Item -LiteralPath $runtimeOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $runtimeOutput -Force | Out-Null
New-Item -ItemType Directory -Path $uiOutput -Force | Out-Null

$commonArguments = @(
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    "/p:Version=$Version"
)
if ($NoRestore) { $commonArguments += '--no-restore' }

dotnet publish $runtimeProject @commonArguments '--output' $runtimeOutput
if ($LASTEXITCODE -ne 0) { throw "Runtime publish failed with exit code $LASTEXITCODE." }

dotnet publish $uiProject @commonArguments '--output' $uiOutput
if ($LASTEXITCODE -ne 0) { throw "UI publish failed with exit code $LASTEXITCODE." }

Write-Host "Published Runtime and external UI layout at $runtimeOutput with version $Version."
