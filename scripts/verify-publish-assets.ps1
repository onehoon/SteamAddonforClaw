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
    'Assets\AppIcon.ico',
    'Dependencies\HidHide\HidHide_1.5.230_x64.exe',
    'Dependencies\UsbIpWin2\USBip-0.9.7.7-x64.exe',
    'Dependencies\Viiper\libVIIPER.dll',
    'Dependencies\Viiper\PROVENANCE.md',
    'Dependencies\Viiper\libVIIPER.h',
    'Dependencies\Viiper\LICENSE.txt'
)

$missingAssets = foreach ($asset in $requiredAssets) {
    $assetPath = Join-Path $PublishDirectory $asset
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        $asset
    }
}

$hidHideInstaller = Join-Path $PublishDirectory 'Dependencies\HidHide\HidHide_1.5.230_x64.exe'
$expectedHidHideSha256 = 'F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6'
if ((Get-FileHash -LiteralPath $hidHideInstaller -Algorithm SHA256).Hash -ne $expectedHidHideSha256) {
    throw 'Published HidHide installer SHA-256 does not match the bundled metadata.'
}

$usbIpInstaller = Join-Path $PublishDirectory 'Dependencies\UsbIpWin2\USBip-0.9.7.7-x64.exe'
$expectedUsbIpSha256 = '51620FA5F9F8BE5932BC9D786DEEE557CE06D5407A99CAB490DCFAC71F185FEA'
if ((Get-FileHash -LiteralPath $usbIpInstaller -Algorithm SHA256).Hash -ne $expectedUsbIpSha256) {
    throw 'Published USB/IP installer SHA-256 does not match the bundled metadata.'
}

$viiperPayload = Join-Path $PublishDirectory 'Dependencies\Viiper\libVIIPER.dll'
# Pins the Phase 2B2 Steam Deck output-callback ABI adoption (VIIPER main@0b362731...) -- see Dependencies/Viiper/PROVENANCE.md.
$expectedViiperSha256 = '304F85467069D48EBCFB7CDA9C50F65A5F8B38C2E7BC597B832A6BA997FA9483'
if ((Get-FileHash -LiteralPath $viiperPayload -Algorithm SHA256).Hash -ne $expectedViiperSha256) {
    throw 'Published VIIPER payload SHA-256 does not match its recorded provenance.'
}

if ($missingAssets) {
    throw "Publish output is missing required WinUI assets: $($missingAssets -join ', ')"
}

Write-Host 'Published WinUI assets verified.'
