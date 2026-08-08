[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PublishDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish directory was not found: $PublishDirectory"
}

$requiredAssets = @(
    'App.xbf',
    'MainWindow.xbf',
    'SteamInputAddonforClaw.pri',
    'Assets\AppIcon.ico'
)

$missingAssets = foreach ($asset in $requiredAssets) {
    $assetPath = Join-Path $PublishDirectory $asset
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        $asset
    }
}

if ($missingAssets) {
    throw "Publish output is missing required WinUI assets: $($missingAssets -join ', ')"
}

Write-Host 'Published WinUI assets verified.'
